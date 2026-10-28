using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class MankiCansTests
{
    private static readonly CharacterDefinition Def = TestHelpers.ResolveSourceDef("manki");
    private static readonly CharacterDefinition Target = TestHelpers.EngineDef;

    private static ServerSimulation Match(bool airborne = false, IMatchRule? rule = null, bool persisted = false)
    {
        var arena = TestHelpers.TestArena(); arena.KillMaxX = 100f;
        var sim = new ServerSimulation(arena, rule ?? NoWinMatchRule.Instance);
        sim.RegisterEntity(1, persisted ? TestHelpers.MankiDef : Def, TestHelpers.PlayerState(20, 20) with
        { PY = Def.CapsuleHeight * .5f + (airborne ? 5f : 0f), IsGrounded = !airborne });
        return sim;
    }

    private static void Step(ServerSimulation sim, InputState input = default, int ticks = 1)
    {
        for (int i = 0; i < ticks; i++) sim.Tick(new() { [1] = input });
    }

    private static MankiCanState Throw(ServerSimulation sim, float distance = 6)
    {
        int count = sim.Resolver.MankiCans.Count;
        Step(sim, new() { ActiveSlot = AbilitySlots.A, IsAiming = true, AimDistance = (ushort)(distance * 100) });
        Step(sim, new() { IsAiming = true, AimDistance = (ushort)(distance * 100) }, 11);
        for (int i = 0; i < 100; i++)
        {
            Step(sim);
            if (sim.Resolver.MankiCans.Count > count) return sim.Resolver.MankiCans[^1];
        }
        throw new Xunit.Sdk.XunitException("Q never left a can on the floor.");
    }

    private static void Ready(ServerSimulation sim)
    {
        for (int i = 0; i < 180 && (sim.GetActiveAbility(1) != null || sim.GetState(1).GetCooldown(AbilitySlots.A) > 0); i++) Step(sim);
        Assert.Null(sim.GetActiveAbility(1));
    }

    private static void PlaceOwner(ServerSimulation sim, float x, float z, float height = 0)
    {
        var state = sim.GetState(1);
        sim.SetState(1, state with { PX = x, PY = Def.CapsuleHeight * .5f + height, PZ = z,
            VX = 0, VY = 0, VZ = 0, FacingYaw = 0, AimYaw = 0, IsGrounded = height == 0 });
    }

    [Fact]
    public void GroundTouchPopsOnceAndLeavesAnArmedCan()
    {
        var sim = Match();
        sim.RegisterEntity(100, Target, TestHelpers.NpcState(20, 25.5f) with { PY = .75f });
        var can = Throw(sim);
        Assert.Equal(240, can.FuseTicksLeft);
        Assert.False(can.IsTriggered);
        var pop = Assert.Single(sim.Resolver.GetActiveHitboxes().Where(h => h.IgnitesOwnerCans));
        Assert.Equal(3f, pop.Damage); Assert.Equal(1.1f, pop.Radius);
        Step(sim, ticks: 8);
        Assert.Equal((ushort)3, sim.GetState(100).DamagePercent);
        Assert.Single(sim.Resolver.MankiCans);
        Assert.False(sim.Resolver.MankiCans[0].IsTriggered);
    }

    [Fact]
    public void FuseProducesTheOrdinaryBlastAndPushesOwnerWithoutDamage()
    {
        var sim = Match(); var can = Throw(sim); Ready(sim);
        PlaceOwner(sim, can.X, can.Z - 1.5f);
        sim.RegisterEntity(100, Target, TestHelpers.NpcState(can.X + 1.5f, can.Z) with { PY = .75f });
        while (sim.Resolver.MankiCans.Count > 0) Step(sim);
        var blast = Assert.Single(sim.Resolver.GetActiveHitboxes().Where(h => h.IgnitesOwnerCans));
        Assert.Equal(10f, blast.Damage); Assert.Equal(3f, blast.Radius);
        Step(sim);
        Assert.Equal((ushort)10, sim.GetState(100).DamagePercent);
        Assert.Equal((ushort)0, sim.GetState(1).DamagePercent);
        Assert.Contains(sim.LastTickHits, h => h.TargetEntityId == 1 && h.ImpactForce > 0);
    }

    [Theory]
    [InlineData(AbilitySlots.F)]
    [InlineData(AbilitySlots.E)]
    [InlineData(AbilitySlots.R)]
    public void OwnSpecialFireOrBlastTriggersWarningThenBiggerBlast(byte slot)
    {
        var sim = Match(); var can = Throw(sim); Ready(sim);
        PlaceOwner(sim, can.X, can.Z - (slot == AbilitySlots.E ? 2.5f : 1.5f));
        if (slot == AbilitySlots.R)
            sim.RegisterEntity(100, Target, TestHelpers.NpcState(can.X, can.Z + 1.5f) with { PY = .75f });
        bool warned = false, bigger = false;
        for (int tick = 0; tick < 100; tick++)
        {
            Step(sim, new() { ActiveSlot = tick == 0 ? slot : (byte)0,
                MoveY = slot == AbilitySlots.E ? 1 : 0, TargetEntityId = slot == AbilitySlots.R ? (byte)100 : (byte)0 });
            warned |= sim.Resolver.MankiCans.Any(c => c.IsTriggered);
            bigger |= sim.Resolver.GetActiveHitboxes().Any(h => h.AttackSlot == AbilitySlots.A && h.Damage == 16 && h.Radius == 4);
        }
        Assert.True(warned); Assert.True(bigger); Assert.Empty(sim.Resolver.MankiCans);
        if (slot != AbilitySlots.R) Assert.Equal((ushort)0, sim.GetState(1).DamagePercent);
    }

    [Fact]
    public void EarlyWarningLastsEightTicksAndDoesNotRestartWhileFlameOverlaps()
    {
        var sim = Match(); var can = Throw(sim); Ready(sim); PlaceOwner(sim, can.X, can.Z - 1.5f);
        Step(sim, new() { ActiveSlot = AbilitySlots.F });
        while (!sim.Resolver.MankiCans[0].IsTriggered) Step(sim);
        Assert.Equal(8, sim.Resolver.MankiCans[0].WarningTicksLeft);
        Step(sim, ticks: 7); Assert.Single(sim.Resolver.MankiCans);
        Step(sim); Assert.Empty(sim.Resolver.MankiCans);
    }

    [Fact]
    public void BlastChainsToASecondCanOutsideFlameReach()
    {
        var sim = Match(); var first = Throw(sim, 4); Ready(sim); var second = Throw(sim, 7); Ready(sim);
        PlaceOwner(sim, first.X, first.Z - 1.5f);
        Step(sim, new() { ActiveSlot = AbilitySlots.F });
        while (!sim.Resolver.MankiCans.Any(c => c.IsTriggered)) Step(sim);
        Assert.False(sim.Resolver.MankiCans.Single(c => c.Id == second.Id).IsTriggered);
        sim.ClearPresentationEvents();
        int blasts = 0;
        for (int i = 0; i < 35; i++)
        {
            Step(sim);
            blasts += sim.GetPresentationEvents(clear: true).Count(e => e.PresentationId == "presentation.manki.round-bomb.explosion");
        }
        Assert.Equal(2, blasts); Assert.Empty(sim.Resolver.MankiCans);
    }

    [Fact]
    public void LaterLandingPopTriggersEarlierCanButNotItself()
    {
        var sim = Match(); var first = Throw(sim); Ready(sim); var second = Throw(sim);
        Step(sim);
        Assert.True(sim.Resolver.MankiCans.Single(c => c.Id == first.Id).IsTriggered);
        Assert.False(sim.Resolver.MankiCans.Single(c => c.Id == second.Id).IsTriggered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OtherOwnersFireAndOrdinaryAttacksDoNotIgnite(bool fire)
    {
        var sim = Match(); var can = Throw(sim);
        sim.Resolver.Spawn(new Hitbox { X = can.X, Y = .35f, Z = can.Z, Radius = 2,
            DurationTicks = 1, OwnerId = fire ? 2UL : 1UL, ActivationId = 900,
            IgnitesOwnerCans = fire, Damage = 3 });
        Step(sim);
        Assert.False(Assert.Single(sim.Resolver.MankiCans).IsTriggered);
    }

    [Fact]
    public void ThreeThrowsSpendThreeChargesAndFourthIsRejected()
    {
        var sim = Match(); Throw(sim, 3); Ready(sim); Throw(sim, 6); Ready(sim); Throw(sim, 9); Ready(sim);
        Assert.Equal(3, sim.Resolver.MankiCans.Count);
        Assert.Equal((byte)3, sim.GetState(1).ChargeStockSpent);
        Step(sim, new() { ActiveSlot = AbilitySlots.A, IsAiming = true });
        Assert.Null(sim.GetActiveAbility(1)); Assert.Equal(3, sim.Resolver.MankiCans.Count);
    }

    [Fact]
    public void EachChargeReturnsOnItsOwnTimer()
    {
        var sim = Match(); Throw(sim, 3); Ready(sim); Throw(sim, 6); Ready(sim); Throw(sim, 9);
        var state = sim.GetState(1);
        Assert.True(state.ChargeStockRegenTicks < state.ChargeStockRegenTicks2);
        Assert.True(state.ChargeStockRegenTicks2 < state.ChargeStockRegenTicks3);
        PlaceOwner(sim, 5, 20);
        int lastReturn = state.ChargeStockRegenTicks3;
        Step(sim, ticks: lastReturn);
        Assert.Equal((byte)0, sim.GetState(1).ChargeStockSpent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotRoundTripAndReplayPreserveCansAndWarning(bool triggered)
    {
        var sim = Match(); var can = Throw(sim); Ready(sim);
        if (triggered)
        {
            PlaceOwner(sim, can.X, can.Z - 1.5f); Step(sim, new() { ActiveSlot = AbilitySlots.F });
            while (!sim.Resolver.MankiCans[0].IsTriggered) Step(sim);
        }
        var frame = new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(),
            Array.Empty<RelayedInput>(), Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>());
        var snapshot = WorldFramePacket.Deserialize(frame.Serialize()).Snapshot;
        var saved = sim.Resolver.MankiCans.ToArray();
        Step(sim, ticks: 100); var expected = sim.CaptureSnapshot();
        sim.RestoreSnapshot(snapshot); Assert.Equal(saved, sim.Resolver.MankiCans);
        Assert.True(snapshot.ContentEquals(sim.CaptureSnapshot()));
        Step(sim, ticks: 100); Assert.True(expected.ContentEquals(sim.CaptureSnapshot()));
    }

    [Fact]
    public void KnockoutRemovesFloorCansAndFlyingCansWithoutBlast()
    {
        var sim = Match(); Throw(sim); Ready(sim);
        Step(sim, new() { ActiveSlot = AbilitySlots.A, IsAiming = true, AimDistance = 900 });
        Step(sim, new() { IsAiming = true, AimDistance = 900 }, 11); Step(sim, ticks: 11);
        Assert.Contains(sim.Resolver.GetActiveHitboxes(), h => h.MankiCan.HasValue);
        sim.SetState(1, sim.GetState(1) with { PX = 1000 }); Step(sim);
        Assert.Empty(sim.Resolver.MankiCans); Assert.Equal(0, sim.Resolver.MankiCanOccupancy(1));
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.AttackSlot == AbilitySlots.A);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetOrRemoveDropsCansWithoutExplosions(bool remove)
    {
        var sim = Match(); Throw(sim); Ready(sim);
        if (remove) sim.RemoveEntity(1); else sim.Resolver.Clear();
        Assert.Empty(sim.Resolver.MankiCans); Assert.Empty(sim.Resolver.DrainPendingExplosions());
    }

    [Fact]
    public void KnockoutOnTheFuseTickSuppressesTheBlast()
    {
        var sim = Match(); Throw(sim);
        while (sim.Resolver.MankiCans[0].FuseTicksLeft > 1) Step(sim);
        sim.ClearPresentationEvents();
        sim.SetState(1, sim.GetState(1) with { PX = 1000 });
        Step(sim);
        Assert.Empty(sim.Resolver.MankiCans);
        Assert.DoesNotContain(sim.GetPresentationEvents(), e => e.PresentationId == "presentation.manki.round-bomb.explosion");
    }

    [Fact]
    public void CanThrownOverVoidExpiresWithoutInventingAFloor()
    {
        var sim = Match(); var payload = Throw(sim).Payload;
        sim.Resolver.Clear(); sim.ClearPresentationEvents();
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, ActivationId = 900, AttackSlot = AbilitySlots.A,
            X = -10, Y = .1f, Z = -10, Radius = .35f, Gravity = 30, DurationTicks = 2,
            MankiCan = payload, Explosion = payload.Blast });
        Step(sim, ticks: 3);
        Assert.Empty(sim.Resolver.MankiCans);
        Assert.DoesNotContain(sim.GetPresentationEvents(), e => e.PresentationId == "presentation.manki.round-bomb.explosion");
    }

    [Fact]
    public void MayhemCallResetRemovesCansAndPendingPopsWithoutBlast()
    {
        var payload = Throw(Match()).Payload;
        var arena = MankiMayhemTests.Arena();
        var sim = new ServerSimulation(arena, new MankiMayhemMatchRule());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = .75f });
        sim.RegisterEntity(2, Target, TestHelpers.PlayerState(2, 0) with { EntityId = 2, PY = .75f });
        sim.BeginMankiMayhem(1337);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, ActivationId = 900, AttackSlot = AbilitySlots.A,
            X = 0, Y = .1f, Z = 0, Radius = .35f, Gravity = 30, DurationTicks = 90,
            MankiCan = payload, Explosion = payload.Blast });
        sim.Resolver.CheckGroundCollision(arena);
        Assert.Single(sim.Resolver.MankiCans);
        Step(sim);
        Assert.Empty(sim.Resolver.MankiCans);
        Assert.DoesNotContain(sim.GetPresentationEvents(), e => e.PresentationId == "presentation.manki.round-bomb.explosion");
    }

    [Fact]
    public void GroundIgnitionActuallyLiftsTheFighterAboveFloorSnapTolerance()
    {
        var sim = Match(); float y = sim.GetState(1).PY;
        Step(sim, new() { ActiveSlot = AbilitySlots.E }); Step(sim, ticks: 8);
        Assert.True(sim.GetState(1).PY > y + .5f);
        Assert.False(sim.GetState(1).IsGrounded);
    }

    [Fact]
    public void FlightSteersKeepsNeutralHeadingAndJumpBurstsWithoutSpendingAirJump()
    {
        var sim = Match(airborne: true); Step(sim, new() { ActiveSlot = AbilitySlots.E }); Step(sim, ticks: 2);
        Assert.Equal(8f, sim.GetState(1).VY);
        Step(sim, new() { MoveX = 1 }); Assert.Equal(12f, sim.GetState(1).VX);
        Step(sim); Assert.Equal(12f, sim.GetState(1).VX);
        Step(sim, new() { MoveY = -1 }); Assert.Equal(-12f, sim.GetState(1).VZ);
        Step(sim, ticks: 12); Assert.Equal(-1.2f, sim.GetState(1).VY);
        byte jumps = sim.GetState(1).JumpsLeft;
        Step(sim, new() { Jump = true }); Assert.Equal(10f, sim.GetState(1).VY);
        Assert.Equal(jumps, sim.GetState(1).JumpsLeft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FuelOutOrSecondEEndsFlightAndNormalAirOptionsReturn(bool manual)
    {
        var sim = Match(airborne: true); Step(sim, new() { ActiveSlot = AbilitySlots.E }); Step(sim, ticks: 3);
        if (manual) Step(sim, new() { ActiveSlot = AbilitySlots.E }); else Step(sim, ticks: 120);
        Assert.Null(sim.GetActiveAbility(1)); Assert.Equal((byte)0, sim.GetState(1).AttackSlot);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State); Assert.Equal((ushort)0, sim.GetState(1).AnimLockTicks);
        Assert.True(sim.GetState(1).GetCooldown(AbilitySlots.E) > 0);
        Step(sim, new() { Jump = true }); Assert.True(sim.GetState(1).VY > 0);
    }

    [Fact]
    public void PoweredFlightAndExhaustReplayExactlyAcrossSnapshot()
    {
        var sim = Match(airborne: true); Step(sim, new() { ActiveSlot = AbilitySlots.E }); Step(sim, new() { MoveX = 1 }, 20);
        var snapshot = sim.CaptureSnapshot();
        void Fly() { Step(sim, new() { MoveY = -1, Jump = true }); Step(sim, ticks: 30); }
        Fly(); var expected = sim.CaptureSnapshot(); sim.RestoreSnapshot(snapshot); Fly();
        Assert.True(expected.ContentEquals(sim.CaptureSnapshot()));
    }

    [Fact]
    public void AerosolChargeWalksAtAuthoredScaleAndKeepsAim()
    {
        var sim = Match(); var before = sim.GetState(1);
        Step(sim, new() { ActiveSlot = AbilitySlots.F, IsAiming = true, MoveX = 1 });
        Step(sim, new() { IsAiming = true, MoveX = 1, AimYaw = 90 }, 30);
        Assert.True(sim.GetState(1).PX > before.PX + .3f);
        Assert.InRange(sim.GetState(1).VX, 0.1f, Def.Movement.RunSpeed * .35f + .001f);
        Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
        Assert.Equal(sim.GetState(1).AimYaw, sim.GetState(1).FacingYaw);
    }

    [Theory]
    [InlineData(FriendlyFireMode.Off, 0)]
    [InlineData(FriendlyFireMode.On, 10)]
    public void CanBlastsFollowCoopFriendlyFireRules(FriendlyFireMode friendlyFire, int damage)
    {
        var sim = new ServerSimulation(SlopballTests.Court(), new SlopballCoopMatchRule(friendlyFire: friendlyFire));
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState(-3, -3) with { PY = .75f });
        sim.BeginSlopballRun(1337);
        var can = Throw(sim, 3); Ready(sim);
        sim.RegisterEntity(100, Target, TestHelpers.NpcState(can.X + 1.5f, can.Z) with { PY = .75f });
        while (sim.Resolver.MankiCans.Count > 0) Step(sim);
        Step(sim, ticks: 8);
        Assert.Equal((ushort)damage, sim.GetState(100).DamagePercent);
    }

    [Fact]
    public void HitstunCancelsFlightAndLeavesNoExhaustHitboxes()
    {
        var sim = Match(airborne: true);
        Step(sim, new() { ActiveSlot = AbilitySlots.E }); Step(sim, ticks: 12);
        sim.SetState(1, sim.GetState(1) with { State = ActionState.Hitstun, HitstunTicks = 10 });
        Step(sim);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.IsCanIgniterOnly);
        Assert.Equal(ActionState.Hitstun, sim.GetState(1).State);
    }

    [Fact]
    public void PersistedPackageExecutesCanFlightAndMobileCharge()
    {
        var sim = Match(persisted: true); var can = Throw(sim); Ready(sim);
        PlaceOwner(sim, can.X, can.Z - 2.5f);
        Step(sim, new() { ActiveSlot = AbilitySlots.E });
        bool burst = false;
        for (int i = 0; i < 35; i++)
        {
            Step(sim, new() { MoveY = 1 });
            burst |= sim.Resolver.GetActiveHitboxes().Any(h => h.Damage == 16 && h.Radius == 4);
        }
        Assert.True(burst);
        Assert.Equal(AuthoringAimMovementMode.Mobile,
            sim.GetDefinition(1).GetCookedSlotAbility(AbilitySlots.F, false)!.AimMovement);
    }
}
