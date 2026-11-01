using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class TargetedLeapTests
{
    private static readonly CookedHitbox Ring = new(AuthoringHitboxShape.Sphere, 2, 0, -.35f, 0, 0, 0, 0,
        null, null, 13, 35, 9, 32, 20, 6, true, 0);
    internal static CookedTargetedLeapCapabilityParameters Parameters => new(18, 6, 24, 18, 12, 6, 60, 18, 68, Ring,
        LandingPresentationId: "presentation.test.slam");
    private static CharacterDefinition Definition()
    {
        var def = TestHelpers.EngineDef; var slots = def.CookedSlots!.ToArray();
        foreach (int n in new[] { 4, 11 })
            slots[n] = new CookedSlotDefinition(n, n == 4 ? "ground.E" : "air.E", n == 11, "Faith", "", "",
                AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, true, false,
                new CookedTimeline(new[] { new CookedStage(128, 0, 0, 0, 0, Array.Empty<string>(),
                    new CookedTimelineOperation[] { new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
                        CharacterPackageCompiler.TargetedLeapCapabilityId, "2", Parameters) }) }));
        return def with { CookedSlots = slots };
    }
    private static ServerSimulation Duel(float distance, ArenaDefinition? arena = null)
    {
        var sim = new ServerSimulation(arena ?? TestHelpers.TestArena()); var def = Definition();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(100, 100) with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(100, 100 + distance) with { PY = TestHelpers.GroundPY(def) });
        return sim;
    }
    private static void Step(ServerSimulation sim, byte slot = 0, float targetStick = 0)
        => sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = slot }, [2] = new() { MoveX = targetStick } });

    [Theory]
    [InlineData(1f)]
    [InlineData(12f)]
    public void FixedBeatsHaveTheSameLandingTimeAtNearAndMaximumRange(float distance)
    {
        var sim = Duel(distance); Step(sim, AbilitySlots.E);
        Assert.NotEqual(ActionState.Aiming, sim.GetState(1).State);
        int land = -1; float highest = 0; int slams = 0;
        for (int t = 1; t < 160; t++)
        {
            Step(sim); highest = MathF.Max(highest, sim.GetState(1).PY);
            var events = sim.GetPresentationEvents(clear: true).Where(e => e.PresentationId == "presentation.test.slam").ToArray();
            if (events.Length > 0) { land = t; slams += events.Length; }
        }
        Assert.InRange(highest - TestHelpers.GroundPY(Definition()), 5.95f, 6.05f);
        Assert.InRange(land, 58, 61);
        Assert.Equal(1, slams);
        Assert.Equal((ushort)13, sim.GetState(2).DamagePercent);
        Assert.Null(sim.GetActiveMove(1));
    }

    [Fact]
    public void HangTracksThenDropLocksTheSpotAndALateRunAvoidsIt()
    {
        var sim = Duel(6); Step(sim, AbilitySlots.E);
        for (int t = 0; t < 20; t++) Step(sim);
        var first = sim.GetActiveMove(1)!.Value.Spot!.Value;
        for (int t = 0; t < 12; t++) Step(sim, targetStick: 1);
        var tracked = sim.GetActiveMove(1)!.Value.Spot!.Value;
        Assert.True(tracked.X > first.X);
        for (int t = 0; t < 10; t++) Step(sim);
        var locked = sim.GetActiveMove(1)!.Value.Spot!.Value;
        for (int t = 0; t < 10; t++)
        {
            Step(sim, targetStick: 1);
            Assert.Equal(locked, sim.GetActiveMove(1)!.Value.Spot!.Value);
        }
        for (int t = 0; t < 120; t++) Step(sim, targetStick: 1);
        Assert.Equal((ushort)0, sim.GetState(2).DamagePercent);
    }

    [Fact]
    public void SnapshotDuringHangReplaysSpotLandingAndHitRecovery()
    {
        var sim = Duel(6); Step(sim, AbilitySlots.E);
        for (int i = 0; i < 25; i++) Step(sim);
        var restored = Duel(6); restored.RestoreSnapshot(sim.CaptureSnapshot());
        for (int i = 0; i < 150; i++)
        {
            Step(sim); Step(restored);
            Assert.True(sim.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()), $"leap replay diverged at {i}");
            Assert.Equal(sim.GetActiveMove(1)?.Spot, restored.GetActiveMove(1)?.Spot);
        }
    }

    [Fact]
    public void VoidFallsWithoutAVisualOrDelayedSlam()
    {
        var arena = TestHelpers.TestArena();
        Array.Fill(arena.Heightmap.Data, float.MinValue);
        arena.KillHeight = -1000;
        var sim = Duel(6, arena); Step(sim, AbilitySlots.E);
        for (int i = 0; i < 180; i++)
        {
            Step(sim);
            Assert.DoesNotContain(sim.GetPresentationEvents(clear: true), e => e.PresentationId == "presentation.test.slam");
        }
        Assert.Null(sim.GetActiveMove(1));
        Assert.True(sim.GetState(1).VY < 0);
    }

    [Fact]
    public void InterruptingTheHangRemovesTheSpotAndNeverSlams()
    {
        var sim = Duel(6); Step(sim, AbilitySlots.E);
        for (int i = 0; i < 25; i++) Step(sim);
        Assert.NotNull(sim.GetActiveMove(1)!.Value.Spot);
        var s = sim.GetState(1); s.State = ActionState.Hitstun; s.HitstunTicks = 30; sim.SetState(1, s);
        for (int i = 0; i < 160; i++)
        {
            Step(sim);
            Assert.Null(sim.GetActiveMove(1));
            Assert.DoesNotContain(sim.GetPresentationEvents(clear: true), e => e.PresentationId == "presentation.test.slam");
        }
    }
    [Theory]
    [InlineData(false, 18)]
    [InlineData(true, 68)]
    public void AHitRecoversQuicklyButAShieldKeepsFullRecovery(bool shield, int recovery)
    {
        var sim = Duel(6); Step(sim, AbilitySlots.E);
        bool landed = false; int activeTicks = 0;
        for (int i = 0; i < 220; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [2] = new() { ShieldHeld = shield } });
            landed |= sim.GetPresentationEvents(clear: true).Any(e => e.PresentationId == "presentation.test.slam");
            if (!landed) continue;
            if (sim.GetActiveAbility(1) == null) break;
            if (sim.GetState(1).HitstopTicks == 0) activeTicks++;
        }
        Assert.True(landed);
        Assert.InRange(activeTicks, recovery - 1, recovery + 1);
        Assert.Equal((ushort)(shield ? 0 : 13), sim.GetState(2).DamagePercent);
    }

    [Fact]
    public void ADashAfterTheSpotLocksEscapesTheSlam()
    {
        var sim = Duel(6); var def = Definition();
        sim.RegisterEntity(3, def, TestHelpers.PlayerState(115, 106) with { PY = TestHelpers.GroundPY(def) });
        Step(sim, AbilitySlots.E);
        for (int i = 0; i < 41; i++) Step(sim);
        var target = sim.GetState(2); target.TargetEntityId = 3; target.LockOn = true; sim.SetState(2, target);
        sim.Tick(new Dictionary<ulong, InputState> { [2] = new() { DashPressed = true, TargetEntityId = 3 } });
        for (int i = 0; i < 100; i++) Step(sim);
        Assert.Equal((ushort)0, sim.GetState(2).DamagePercent);
    }

    [Fact]
    public void SweptDropLandsOnAThinRaisedPlatform()
    {
        var arena = TestHelpers.TestArena();
        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = 90, AY = 0, AZ = 90, BX = 90, BY = 0, BZ = 120, CX = 120, CY = 0, CZ = 90 },
            new CollisionTriangle { AX = 90, AY = 0, AZ = 120, BX = 120, BY = 0, BZ = 120, CX = 120, CY = 0, CZ = 90 },
            new CollisionTriangle { AX = 98, AY = 3, AZ = 104, BX = 98, BY = 3, BZ = 108, CX = 102, CY = 3, CZ = 104 },
            new CollisionTriangle { AX = 98, AY = 3, AZ = 108, BX = 102, BY = 3, BZ = 108, CX = 102, CY = 3, CZ = 104 },
        };
        arena.MinX = arena.MinZ = 90; arena.MaxX = arena.MaxZ = 120;
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        var sim = Duel(6, arena); var target = sim.GetState(2); target.PY += 3; sim.SetState(2, target);
        Step(sim, AbilitySlots.E);
        var landings = new List<TimelinePresentationEvent>();
        for (int i = 0; i < 160; i++)
        {
            Step(sim); landings.AddRange(sim.GetPresentationEvents(clear: true).Where(e => e.PresentationId == "presentation.test.slam"));
        }
        var landing = Assert.Single(landings);
        Assert.InRange(landing.WorldY, 2.98f, 3.02f);
        Assert.InRange(sim.GetState(1).PY - Definition().CapsuleHeight * .5f, 2.98f, 3.02f);
    }

    [Fact]
    public void ADropOntoATargetAtTheLedgeLandsInsteadOfStoppingOverTheVoid()
    {
        var arena = TestHelpers.TestArena();
        Array.Fill(arena.Heightmap.Data, float.MinValue);
        arena.KillHeight = -1000; arena.KillTop = 1000;
        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = 90, AY = 0, AZ = 90, BX = 90, BY = 0, BZ = 106.3f, CX = 110, CY = 0, CZ = 90 },
            new CollisionTriangle { AX = 90, AY = 0, AZ = 106.3f, BX = 110, BY = 0, BZ = 106.3f, CX = 110, CY = 0, CZ = 90 },
            new CollisionTriangle { AX = 90, AY = 0, AZ = 112, BX = 90, BY = 0, BZ = 120, CX = 110, CY = 0, CZ = 112 },
            new CollisionTriangle { AX = 90, AY = 0, AZ = 120, BX = 110, BY = 0, BZ = 120, CX = 110, CY = 0, CZ = 112 },
        };
        arena.MinX = arena.MinZ = 90; arena.MaxX = arena.MaxZ = 120;
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        var sim = new ServerSimulation(arena); var def = Definition();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(100, 114) with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(100, 106) with { PY = TestHelpers.GroundPY(def) });
        Step(sim, AbilitySlots.E);
        int slams = 0;
        for (int i = 0; i < 160; i++)
        {
            Step(sim); slams += sim.GetPresentationEvents(clear: true).Count(e => e.PresentationId == "presentation.test.slam");
        }
        Assert.Equal(1, slams);
        Assert.Equal((ushort)13, sim.GetState(2).DamagePercent);
        Assert.True(sim.GetState(1).IsGrounded);
        Assert.InRange(sim.GetState(1).PZ, 90f, 106.3f);
    }
}
