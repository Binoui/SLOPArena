using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class SteeredChargeTests
{
    private static CookedHitbox Contact => new(AuthoringHitboxShape.Sphere, .65f, 0, 0, 1, 0, 0, 0,
        null, null, 3, 40, 8, 30, 20, 1, false, 0);
    internal static CookedSteeredChargeCapabilityParameters Parameters { get; } = new(
        18, 240, 90, true, Contact, 18, 30, 12, false, 45, 20, 9, 240, 420, Contact,
        new CookedSteeredChargeCarry(1.1f, .4f, 36, new CookedGrabLaunch(12, 40, 12, 36, 20), 18));

    internal static CharacterDefinition Definition(CookedSteeredChargeCapabilityParameters? parameters = null)
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        foreach (int n in new[] { 5, 12 })
            slots[n] = new CookedSlotDefinition(n, n == 5 ? "ground.R" : "air.R", n == 12, "Joust", "", "",
                AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
                new CookedTimeline(new[] { new CookedStage(18, 0, 0, 0, 0, Array.Empty<string>(),
                    new CookedTimelineOperation[] { new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
                        CharacterPackageCompiler.SteeredChargeCapabilityId, "2", parameters ?? Parameters) }) }));
        return def with { CookedSlots = slots };
    }
    internal static ServerSimulation Duel(bool air = false, bool challenge = false, ArenaDefinition? arena = null)
    {
        var sim = new ServerSimulation(arena ?? TestHelpers.TestArena()); var def = Definition();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(100, 100) with { PY = air ? 20 : TestHelpers.GroundPY(def),
            IsGrounded = !air, Challenge = challenge ? new Challenge(2, 0, 120) : default });
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(100, 104) with { PY = air ? 20 : TestHelpers.GroundPY(def), IsGrounded = !air });
        if (air) sim.NoGravityEntityId = 2;
        return sim;
    }
    internal static void Step(ServerSimulation sim, byte slot = 0, bool shield = false)
        => sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = slot }, [2] = new() { ShieldHeld = shield } });

    private static CharacterDefinition AssistedDefinition(bool assist, ushort gallop = 150)
    {
        return Definition(Parameters with { MaxGallopTicks = gallop,
            Assist = assist ? new ChargeAssistParameters(3.5f, 70, 900) : null });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AssistCatchesOffsetTargetDespiteStickAway(bool assist, bool stickAway)
    {
        var def = AssistedDefinition(assist); var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(100, 100) with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(100, 110) with { PY = TestHelpers.GroundPY(def) });
        Step(sim, AbilitySlots.R);
        for (int i = 0; i < 18; i++) Step(sim);
        var target = sim.GetState(2);
        sim.SetState(2, target with { PX = 101.2f, PZ = sim.GetState(1).PZ + 1.0f });
        bool caught = false;
        for (int i = 0; i < 20; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { MoveX = stickAway ? -1 : 0 }, [2] = default });
            caught |= sim.GetState(2).State == ActionState.CommandGrabbed;
        }
        Assert.Equal(assist, caught);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DodgeBehindEscapesWithoutTurningBack(bool air)
    {
        var def = AssistedDefinition(true); var sim = new ServerSimulation(TestHelpers.TestArena());
        var owner = TestHelpers.PlayerState(100, 100) with { PY = air ? 20 : TestHelpers.GroundPY(def), IsGrounded = !air };
        sim.RegisterEntity(1, def, owner);
        sim.RegisterEntity(2, def, owner with { EntityId = 2, PZ = 110 });
        if (air) sim.NoGravityEntityId = 2;
        Step(sim, AbilitySlots.R);
        for (int t = 0; t < 18; t++) Step(sim);
        sim.SetState(2, sim.GetState(2) with { PX = 101.2f, PZ = 98 });
        for (int t = 0; t < 20; t++) Step(sim);
        Assert.Equal((ushort)0, sim.GetState(2).DamagePercent);
        TestHelpers.AssertNear(0, sim.GetState(1).FacingYaw, .001f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotDuringAssistedTurnReplaysIdentically(bool air)
    {
        var def = AssistedDefinition(true);
        ServerSimulation Make()
        {
            var sim = new ServerSimulation(TestHelpers.TestArena());
            var owner = TestHelpers.PlayerState(100, 100) with { PY = air ? 20 : TestHelpers.GroundPY(def), IsGrounded = !air };
            sim.RegisterEntity(1, def, owner);
            sim.RegisterEntity(2, def, owner with { EntityId = 2, PZ = 110 });
            if (air) sim.NoGravityEntityId = 2;
            return sim;
        }
        var sim = Make(); Step(sim, AbilitySlots.R);
        for (int t = 0; t < 18; t++) Step(sim);
        sim.SetState(2, sim.GetState(2) with { PX = 101.2f, PZ = sim.GetState(1).PZ + 1 });
        Step(sim);
        var restored = Make(); restored.RestoreSnapshot(sim.CaptureSnapshot());
        for (int t = 0; t < 80; t++)
        {
            Step(sim); Step(restored);
            Assert.True(sim.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()), $"assist replay diverged at {t}");
        }
    }

    [Fact]
    public void AssistPrefersSelectedTargetThenNearestWithStableTiesAndIgnoresBehind()
    {
        var owner = TestHelpers.PlayerState(100, 100);
        var near = owner with { EntityId = 2, PX = 101, PZ = 102 };
        var selected = owner with { EntityId = 3, PX = 99, PZ = 103 };
        var a = new ChargeAssistParameters(3.5f, 70, 900);
        Assert.Equal(MathF.Atan2(-1, 3), Abilities.SteeredChargeAbility.AssistHeading(owner, 0, 3, a, new[] { near, selected }));
        Assert.Equal(MathF.Atan2(1, 2), Abilities.SteeredChargeAbility.AssistHeading(owner, 0, 0, a, new[] { selected, near }));
        Assert.Null(Abilities.SteeredChargeAbility.AssistHeading(owner, 0, 3, a, new[] { selected with { PZ = 99 } }));
        Assert.Equal(MathF.Atan2(1, 2), Abilities.SteeredChargeAbility.AssistHeading(owner, 0, 0, a,
            new[] { near with { EntityId = 4, PX = 99 }, near }));
    }

    [Fact]
    public void LongerGallopTravelsEighteenMetresFarther()
    {
        float Travel(ushort ticks)
        {
            var def = AssistedDefinition(true, ticks); var sim = new ServerSimulation(TestHelpers.TestArena());
            sim.RegisterEntity(1, def, TestHelpers.PlayerState(100, 100) with { PY = TestHelpers.GroundPY(def) });
            Step(sim, AbilitySlots.R);
            for (int i = 0; i < 220; i++) Step(sim);
            return sim.GetState(1).PZ;
        }
        TestHelpers.AssertNear(18f, Travel(150) - Travel(90), .02f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScoopsCarriesThenConsumesSameSlotPressAndFlingsOnce(bool air)
    {
        var sim = Duel(air); Step(sim, AbilitySlots.R);
        for (int i = 0; i < 16; i++) Step(sim);
        Assert.InRange(sim.GetState(1).PZ, 99.99f, 100.01f);
        for (int i = 0; i < 40 && sim.GetState(2).State != ActionState.CommandGrabbed; i++) Step(sim);
        Assert.Equal(ActionState.CommandGrabbed, sim.GetState(2).State);
        Assert.Equal((ushort)3, sim.GetState(2).DamagePercent);
        float z = sim.GetState(1).PZ;
        for (int i = 0; i < 4; i++) Step(sim);
        Assert.True(sim.GetState(1).PZ > z + .8f);
        Assert.InRange(sim.GetState(2).PZ - sim.GetState(1).PZ, 1.05f, 1.15f);
        Step(sim, AbilitySlots.R);
        var fling = Assert.Single(sim.LastTickHits.Where(h => h.TargetEntityId == 2));
        Assert.Equal(12, fling.Damage);
        Assert.Equal(AbilitySlots.R, fling.AttackSlot);
        Assert.Equal((ulong)0, sim.GetState(1).InteractionId);
        for (int i = 0; i < 80; i++) Step(sim);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal((ushort)15, sim.GetState(2).DamagePercent);
    }

    [Fact]
    public void ChallengeHalvesRearAndIsConsumedEvenOnAMiss()
    {
        var sim = Duel(challenge: true); Step(sim, AbilitySlots.R);
        Assert.Equal(default, sim.GetState(1).Challenge);
        for (int i = 0; i < 12; i++) Step(sim);
        Assert.True(sim.GetState(1).PZ > 100.4f);
    }

    [Fact]
    public void ShieldBlocksTheScoop()
    {
        var sim = Duel(); Step(sim, AbilitySlots.R, true);
        bool blocked = false;
        for (int i = 0; i < 90; i++) { Step(sim, shield: true); blocked |= sim.LastTickHits.Any(h => h.Blocked); }
        Assert.True(blocked);
        Assert.Equal((ushort)0, sim.GetState(2).DamagePercent);
        Assert.Equal((ulong)0, sim.GetState(2).InteractionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotMidCarryReplaysTheSameTimedFling(bool air)
    {
        var sim = Duel(air); Step(sim, AbilitySlots.R);
        for (int i = 0; i < 60 && sim.GetState(2).State != ActionState.CommandGrabbed; i++) Step(sim);
        Assert.Equal(ActionState.CommandGrabbed, sim.GetState(2).State);
        for (int i = 0; i < 5; i++) Step(sim);
        var restored = Duel(air); restored.RestoreSnapshot(sim.CaptureSnapshot());
        for (int i = 0; i < 100; i++)
        {
            Step(sim); Step(restored);
            Assert.True(sim.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()), $"carry replay diverged at {i}");
        }
        Assert.Equal((ushort)15, sim.GetState(2).DamagePercent);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CarryStopsAndFlingsAtTheGroundLedgeOrWall(bool wall)
    {
        var arena = TestHelpers.TestArena();
        if (wall)
        {
            arena.CollisionTriangles = new[]
            {
                new CollisionTriangle { AX = 90, AY = 0, AZ = 90, BX = 90, BY = 0, BZ = 120, CX = 120, CY = 0, CZ = 90 },
                new CollisionTriangle { AX = 90, AY = 0, AZ = 120, BX = 120, BY = 0, BZ = 120, CX = 120, CY = 0, CZ = 90 },
                new CollisionTriangle { AX = 90, AY = 0, AZ = 106, BX = 90, BY = 8, BZ = 106, CX = 120, CY = 0, CZ = 106 },
                new CollisionTriangle { AX = 120, AY = 8, AZ = 106, BX = 120, BY = 0, BZ = 106, CX = 90, CY = 8, CZ = 106 },
            };
            arena.MinX = arena.MinZ = 90; arena.MaxX = arena.MaxZ = 120;
            arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        }
        else
            for (int z = 106; z < arena.Heightmap.Height; z++)
                for (int x = 0; x < arena.Heightmap.Width; x++) arena.Heightmap.Data[z * arena.Heightmap.Width + x] = float.MinValue;
        var sim = Duel(arena: arena); Step(sim, AbilitySlots.R);
        for (int i = 0; i < 50 && sim.GetState(2).State != ActionState.CommandGrabbed; i++) Step(sim);
        Assert.True(sim.GetState(2).State == ActionState.CommandGrabbed, $"holder {sim.GetState(1).PX}, {sim.GetState(1).PY}, {sim.GetState(1).PZ}, {sim.GetState(1).State}; victim {sim.GetState(2).PX}, {sim.GetState(2).PY}, {sim.GetState(2).PZ}, damage {sim.GetState(2).DamagePercent}");
        for (int i = 0; i < 30 && sim.GetState(1).InteractionId != 0; i++) Step(sim);
        Assert.Equal((ulong)0, sim.GetState(1).InteractionId);
        Assert.Equal((ushort)15, sim.GetState(2).DamagePercent);
        Assert.True(sim.GetState(1).IsGrounded);
        Assert.True(sim.GetState(1).PZ < 106);

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeutralGroundTrackingGivesUpAfterAPassAndAirNeverTracks(bool air)
    {
        var sim = Duel(air); Step(sim, AbilitySlots.R);
        for (int i = 0; i < 19; i++) Step(sim);
        var target = sim.GetState(2); target.PX = 106; target.PZ = 110; sim.SetState(2, target);
        Step(sim);
        Assert.Equal(air, MathF.Abs(sim.GetState(1).FacingYaw) < .001f);
        target.PZ = 90; sim.SetState(2, target); Step(sim);
        float yaw = sim.GetState(1).FacingYaw;
        target.PZ = 110; sim.SetState(2, target); Step(sim);
        Assert.Equal(yaw, sim.GetState(1).FacingYaw);
    }

    [Fact]
    public void BothVolumesRetireWhenTheFirstOfTwoFightersIsScooped()
    {
        var sim = Duel(); var def = Definition();
        sim.RegisterEntity(3, def, TestHelpers.PlayerState(100.05f, 104) with { PY = TestHelpers.GroundPY(def) });
        Step(sim, AbilitySlots.R);
        for (int i = 0; i < 50 && sim.GetState(1).InteractionId == 0; i++) Step(sim);
        var held = sim.GetState(1).InteractionPartnerId;
        Assert.NotEqual(0UL, held);
        Assert.Equal((ushort)3, sim.GetState(held).DamagePercent);
        Assert.Equal((ushort)0, sim.GetState(held == 2 ? 3UL : 2UL).DamagePercent);
    }

    [Fact]
    public void AThirdPartyHitOnThePassengerBreaksThePairWithThatHitsReaction()
    {
        var sim = Duel(); Step(sim, AbilitySlots.R);
        for (int i = 0; i < 50 && sim.GetState(2).State != ActionState.CommandGrabbed; i++) Step(sim);
        var def = Definition();
        sim.RegisterEntity(3, def, TestHelpers.PlayerState(105, 105) with { PY = TestHelpers.GroundPY(def) });
        var target = sim.GetState(2);
        sim.Resolver.Spawn(new Hitbox { X = target.PX, Y = target.PY, Z = target.PZ, Radius = 2,
            Shape = HitboxShape.Sphere, Damage = 7, StunTicks = 20, BaseKnockback = 12,
            DurationTicks = 1, OwnerId = 3, OnlyTargetId = 2 });
        Step(sim);
        Assert.Equal(0UL, sim.GetState(1).InteractionId);
        Assert.NotEqual(ActionState.CommandGrabbed, sim.GetState(2).State);
        Assert.Equal((ushort)10, sim.GetState(2).DamagePercent);
        Assert.Contains(sim.LastTickHits, h => h.OwnerEntityId == 3 && h.TargetEntityId == 2 && h.Damage == 7);
        for (int i = 0; i < 80; i++) Step(sim);
        Assert.Equal((ushort)10, sim.GetState(2).DamagePercent);
    }

    [Fact]
    public void ArmourOnTheRiderKeepsTheCarryThroughADamagingHit()
    {
        var sim = Duel(); Step(sim, AbilitySlots.R);
        for (int i = 0; i < 50 && sim.GetState(2).State != ActionState.CommandGrabbed; i++) Step(sim);
        var def = Definition();
        sim.RegisterEntity(3, def, TestHelpers.PlayerState(105, 105) with { PY = TestHelpers.GroundPY(def) });
        var owner = sim.GetState(1); ulong pair = owner.InteractionId;
        sim.Resolver.Spawn(new Hitbox { X = owner.PX, Y = owner.PY, Z = owner.PZ, Radius = 2,
            Shape = HitboxShape.Sphere, Damage = 7, StunTicks = 20, BaseKnockback = 12,
            DurationTicks = 1, OwnerId = 3, OnlyTargetId = 1 });
        Step(sim);
        Assert.Equal(pair, sim.GetState(1).InteractionId);
        Assert.Equal(ActionState.CommandGrabbed, sim.GetState(2).State);
        Assert.Equal((ushort)7, sim.GetState(1).DamagePercent);
        Step(sim, AbilitySlots.R);
        for (int i = 0; i < 20 && sim.GetState(1).InteractionId != 0; i++) Step(sim);
        Assert.Equal((ushort)15, sim.GetState(2).DamagePercent);
    }

    [Fact]
    public void AnArmouredVictimTakesTheContactWithoutBecomingAPassenger()
    {
        var def = Definition(); var slots = def.CookedSlots!.ToArray();
        slots[0] = new CookedSlotDefinition(0, "ground.1", false, "Armour fixture", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[] { new CookedStage(120, 0, 0, 0, 0, Array.Empty<string>(),
                new CookedTimelineOperation[] { new CookedArmorWindowOperation(0, AuthoringUnit.Ticks, 120) }) }));
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(100, 100) with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(2, def with { CookedSlots = slots }, TestHelpers.PlayerState(100, 104) with { PY = TestHelpers.GroundPY(def) });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = AbilitySlots.R }, [2] = new() { ActiveSlot = AbilitySlots.Normal1 } });
        Assert.True(sim.GetActiveAbility(2)?.HasArmor == true, $"initial target {sim.GetState(2).State}, slot {sim.GetState(2).AttackSlot}");
        for (int i = 0; i < 80; i++)
        {
            bool armour = sim.GetActiveAbility(2)?.HasArmor == true;
            Step(sim);
            Assert.True(sim.GetState(2).State != ActionState.CommandGrabbed,
                $"tick {i}, armour before {armour}, target move {sim.GetState(2).AttackSlot}, damage {sim.GetState(2).DamagePercent}");
        }
        Assert.Equal((ushort)3, sim.GetState(2).DamagePercent);
        Assert.Equal(0UL, sim.GetState(1).InteractionId);
    }

    [Fact]
    public void AGrabOnTheRiderFreesThePassengerWithoutFlingingIt()
    {
        var sim = Duel(); Step(sim, AbilitySlots.R);
        for (int i = 0; i < 50 && sim.GetState(2).State != ActionState.CommandGrabbed; i++) Step(sim);
        var owner = sim.GetState(1);
        var def = Definition() with { CaptureGeometry = new CookedCaptureGeometry(1.5f, .8f, 1.2f, .6f,
            new CaptureAnchor(0, .6f, .2f), new CaptureAnchor(0, .6f, .65f)) };
        sim.RegisterEntity(3, def, TestHelpers.PlayerState(owner.PX + 1.2f, owner.PZ + .3f) with {
            PY = TestHelpers.GroundPY(def), State = ActionState.GrabAttempt, InteractionPhase = (byte)DefenseInteractionPhase.Attempt,
            StateTicks = DefenseConfig.GrabWhiffRecoveryTicks + DefenseConfig.GrabActiveTicks,
            FacingYaw = -MathF.PI * .5f, CapturedYaw = -9000 });
        Step(sim);
        Assert.Equal(ActionState.Grabbed, sim.GetState(1).State);
        Assert.Equal(3UL, sim.GetState(1).InteractionPartnerId);
        Assert.Equal(0UL, sim.GetState(2).InteractionId);
        Assert.Equal((ushort)3, sim.GetState(2).DamagePercent);
        Assert.NotEqual(ActionState.CommandGrabbed, sim.GetState(2).State);
    }

}
